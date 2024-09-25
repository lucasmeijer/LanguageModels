using Microsoft.Extensions.Configuration;

namespace LanguageModels;

public record GroqApiKey(string ApiKey)
{
    // ReSharper disable once UnusedMember.Global
    public GroqApiKey(IConfiguration configuration) : this(configuration.GetMandatory("GROQ_API_KEY"))
    {
    }
}