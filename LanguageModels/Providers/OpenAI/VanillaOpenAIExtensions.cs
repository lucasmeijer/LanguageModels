using Microsoft.Extensions.DependencyInjection;

namespace LanguageModels;

public static class VanillaOpenAIExtensions
{
    internal static void SetupVanillaOpenAI(this IServiceCollection serviceCollection)
    {
        serviceCollection.SetupOpenAIHttpClient();
        serviceCollection.AddSingleton<OpenAIModels>();
    }

    public static void SetupOpenAIHttpClient(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<OpenAIApiKey>();
        serviceCollection.AddHttpClient<VanillaOpenAIHttpClient>((provider, client) =>
        {
            VanillaOpenAI.SetupHttpClient(client, provider.GetRequiredService<OpenAIApiKey>().ApiKey);
        });
    }
}