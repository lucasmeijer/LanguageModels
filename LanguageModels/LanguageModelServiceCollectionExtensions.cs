using Microsoft.Extensions.DependencyInjection;

namespace LanguageModels;

public static class LanguageModelExtensions
{
    public static void AddLanguageModels(this IServiceCollection serviceCollection)
    {
        serviceCollection.SetupAnthropic();
        serviceCollection.SetupVanillaOpenAI();
        serviceCollection.SetupGroq();
        serviceCollection.SetupAzure();
        serviceCollection.SetupAmazonBedrock();
    }
}