using System.Net;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LanguageModels;

class AnthropicModel(AnthropicHttpClient anthropicHttpClient, string model) : ILanguageModel
{
    string Model { get; } = model;

    public string Identifier => Model;
    public bool SupportFunctionCalls => true;
    public bool SupportImageInputs => true;
    
    public IExecutionInProgress Execute(ChatRequest request, CancellationToken cancellationToken) => new ExecutionInProgress(request, ExecuteImpl);

    async IAsyncEnumerable<JsonElement> SseStreamFor(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sseParser = SseParser.Create(stream, (_, bytes) => JsonSerializer.Deserialize<JsonElement>(bytes));

        await foreach (var sseItem in sseParser.EnumerateAsync(cancellationToken))
            yield return sseItem.Data;
    }
    
    async Task ExecuteImpl(ChatRequest request, Func<string, CancellationToken, Task> textSegmentWriter, Func<IMessage, CancellationToken, Task> completeMessageWriter, CancellationToken cancellationToken)
    {
        var jsonString = AnthropicProtocol.RequestObjectFor(request, Model).ToJsonString();
        using var httpResponse = await anthropicHttpClient.HttpClient.SendAsync(new(HttpMethod.Post, "")
        {
            Content = new StringContent(jsonString, Encoding.UTF8, "application/json"),
            Headers =
            {
                Accept = { new("text/event-stream") }, 
            }
        }, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorObject = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            var extra = httpResponse.StatusCode == HttpStatusCode.BadRequest ? jsonString[..500] : "";
            throw new HttpRequestException($"{httpResponse.StatusCode} {errorObject} {extra}");
        }
        
        await using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        var sseStream = SseStreamFor(stream, cancellationToken);
        
        await AnthropicProtocol.ProcessResponse(request, sseStream, textSegmentWriter, completeMessageWriter, cancellationToken);
    }
}