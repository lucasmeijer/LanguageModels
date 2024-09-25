using Microsoft.Extensions.Configuration;

namespace LanguageModels;

public record AzureCredentials(
    string Key,
    string ResourceName,
    string DeploymentName,
    string ApiVersion,
    bool SupportImageInputs)
{
    // ReSharper disable once UnusedMember.Global
    public AzureCredentials(IConfiguration configuration) : this(configuration.GetMandatory("AZURE_OPENAI_KEY_FRANCE"), "professionals-openai-france", "gpt4", "2024-02-15-preview", false)
    {
    }
}