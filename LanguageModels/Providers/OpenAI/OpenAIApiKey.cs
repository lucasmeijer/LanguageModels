using Microsoft.Extensions.Configuration;

namespace LanguageModels;

public record OpenAIApiKey(string ApiKey)
{
    // ReSharper disable once UnusedMember.Global
    public OpenAIApiKey(IConfiguration configuration) : this(configuration.GetMandatory("OPENAI_API_KEY"))
    {
    }
}