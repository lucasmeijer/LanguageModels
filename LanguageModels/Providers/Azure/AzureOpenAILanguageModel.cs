namespace LanguageModels;

public class AzureOpenAILanguageModel(
    AzureHttpClient azureHttpClient,
    AzureCredentials azureCredentials, bool supportsStreaming = true) : OpenAILanguageModelBase(azureHttpClient.HttpClient, supportsStreaming)
{
    public override bool SupportImageInputs => azureCredentials.SupportImageInputs;
    
    public override string Identifier => $"azure_{azureCredentials.ResourceName}_{azureCredentials.DeploymentName}";
    protected override string Url => $"chat/completions?api-version={azureCredentials.ApiVersion}";
}