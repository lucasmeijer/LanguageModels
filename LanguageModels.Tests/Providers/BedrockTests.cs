using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace LanguageModels.Tests;

[TestFixture]
public class BedrockTest : LanguageModelTestBase
{
    protected override void SetupLanguageModelIn(IServiceCollection serviceCollection)
    {
        serviceCollection.AddScoped<ILanguageModel>(sp => ActivatorUtilities.CreateInstance<BedrockModels>(sp).Sonnet35);
    }
}