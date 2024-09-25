using Microsoft.Extensions.Configuration;

namespace LanguageModels;

public sealed record AnthropicApiKey(string ApiKey)
{
    public AnthropicApiKey(IConfiguration configuration) : this(configuration.GetMandatory("ANTHROPIC_API_KEY"))
    {
    }
}