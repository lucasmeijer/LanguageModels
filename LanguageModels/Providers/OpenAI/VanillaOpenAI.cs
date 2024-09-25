using System.Net;
using System.Text.Json.Nodes;

namespace LanguageModels;

public class OpenAIModels(VanillaOpenAIHttpClient httpClient)
{
    public ILanguageModel Gpt4 { get; } = new VanillaOpenAI(httpClient, "gpt-4");
    public ILanguageModel Gpt4o { get; } = new VanillaOpenAI(httpClient, "gpt-4o");
    public ILanguageModel Gpt35Turbo { get; } = new VanillaOpenAI(httpClient, "gpt-3.5-turbo");
    public ILanguageModel O1Preview { get; } = new O1Preview(httpClient);
}


class O1Preview(VanillaOpenAIHttpClient httpClient) : VanillaOpenAI(httpClient, "o1-preview", false)
{
    protected override JsonObject JsonElementForRequest(ChatRequest request)
    {
        if (request.SystemPrompt != null)
            request = request with
            {
                SystemPrompt = null,
                Messages =
                [
                    new ChatMessage("user", request.SystemPrompt),
                    ..request.Messages
                ]
            };

        if (request.Temperature != 1)
            request = request with { Temperature = 1 };
        
        return base.JsonElementForRequest(request);
    }
}

class VanillaOpenAI : OpenAILanguageModelBase
{
    string _model;
    
    //real openai
    public VanillaOpenAI(VanillaOpenAIHttpClient httpClient, string model, bool supportsStreaming = true) : base(httpClient.HttpClient, supportsStreaming)
    {
        _model = model;
    }
    
    internal VanillaOpenAI(HttpClient httpClient, string model, bool supportsStreaming = true) : base(httpClient, supportsStreaming)
    {
        _model = model;
    }
    
    public VanillaOpenAI(string model, string apikey, bool supportsStreaming = true) : base(SetupHttpClient(new(), apikey), supportsStreaming)
    {
        _model = model;
    }
    
    protected override string Url => "chat/completions";
    public override string Identifier => _model;

    public static HttpClient SetupHttpClient(HttpClient client, string apiKey)
    {
        client.BaseAddress = new("https://api.openai.com/v1/");
        client.DefaultRequestHeaders.Authorization = new("Bearer", apiKey);
        return client;
    }
}