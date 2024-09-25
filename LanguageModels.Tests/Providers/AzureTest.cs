using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace LanguageModels.Tests;

[TestFixture]
public class AzureTest : LanguageModelTestBase
{
    protected override void SetupLanguageModelIn(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton(new AzureCredentials(Configuration.GetMandatory("AZURE_OPENAI_KEY_FRANCE"),"professionals-openai-france", "gpt4", "2024-02-15-preview",false));
        serviceCollection.AddScoped<ILanguageModel, AzureOpenAILanguageModel>();
    }
}