using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace LanguageModels.Tests;

[TestFixture]
public class VanillaOpenAIProductionTest : LanguageModelTestBase
{
    protected override void SetupLanguageModelIn(IServiceCollection serviceCollection)
    {
        serviceCollection.AddScoped<ILanguageModel>(sp => ActivatorUtilities.CreateInstance<OpenAIModels>(sp).Gpt4o);
    }

    [Test]
    public async Task Strawberry()
    {
        var model = ServiceProvider.GetRequiredService<OpenAIModels>().O1Preview;
        await using var executionInProgress = model.Execute(new ChatRequest()
        {
            Messages = [new ChatMessage("user", "What is 4 plus 4")],
        }, default);
        await foreach (var line in executionInProgress.ReadTextSegmentsAsync())
        {
            Console.WriteLine(line);
        }
    }
}


[TestFixture]
[Explicit]
public class O1PreviewTests : LanguageModelTestBase
{
    protected override void SetupLanguageModelIn(IServiceCollection serviceCollection)
    {
        serviceCollection.AddScoped<ILanguageModel>(sp => ActivatorUtilities.CreateInstance<OpenAIModels>(sp).O1Preview);
    }

    [Test]
    public async Task Strawberry()
    {
        var model = ServiceProvider.GetRequiredService<OpenAIModels>().O1Preview;
        await using var executionInProgress = model.Execute(new ChatRequest()
        {
            Messages = [new ChatMessage("user", "What is 4 plus 4")],
        }, default);
        await foreach (var line in executionInProgress.ReadTextSegmentsAsync())
        {
            Console.WriteLine(line);
        }

        await foreach (var msg in executionInProgress.ReadCompleteMessagesAsync())
        {
            Console.WriteLine(msg);
        }
    }
}