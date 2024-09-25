namespace LanguageModels;

public class AnthropicLanguageModels(AnthropicHttpClient anthropicHttpClient)
{
    public ILanguageModel Sonnet35 { get; } = new AnthropicModel(anthropicHttpClient, "claude-3-5-sonnet-20240620");
    public ILanguageModel Sonnet3 { get; } = new AnthropicModel(anthropicHttpClient, "claude-3-sonnet-20240229");
    public ILanguageModel Haiku3 { get; } = new AnthropicModel(anthropicHttpClient, "claude-3-haiku-20240307");
    public ILanguageModel Opus3 { get; } = new AnthropicModel(anthropicHttpClient, "claude-3-opus-20240229");
}