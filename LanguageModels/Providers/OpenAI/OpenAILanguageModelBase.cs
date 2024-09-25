using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LanguageModels;

public abstract class OpenAILanguageModelBase(HttpClient httpClient, bool supportsStreaming) : ILanguageModel
{
    public abstract string Identifier { get; }
    public virtual bool SupportFunctionCalls => true;
    public virtual bool SupportImageInputs => true;
    
    public IExecutionInProgress Execute(ChatRequest request, CancellationToken cancellationToken) => new ExecutionInProgress(request, ExecuteImpl);

    struct ChunkOrDone
    {
        public JsonElement Json { get; set; }
        public bool Done { get; set; }
    }
    
    async Task ExecuteImpl(ChatRequest request, Func<string, CancellationToken, Task> textSegmentWriter, Func<IMessage, CancellationToken, Task> completeMessageWriter, CancellationToken cancellationToken)
    {
        var jsonString = JsonElementForRequest(request).ToJsonString();
        //Console.WriteLine(jsonString);
        var response = httpClient.SendAsync(new(HttpMethod.Post, Url)
        {
            Content = new StringContent(jsonString, Encoding.UTF8, "application/json"),
            Headers =
            {
                Accept = { new("text/event-stream") },
            }
        }, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        using var httpResponse = await response;
        
        if (!httpResponse.IsSuccessStatusCode)
        {
            var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(body, null, httpResponse.StatusCode);
        }

        if (supportsStreaming)
            await HandleStreamingResponse(httpResponse, textSegmentWriter, completeMessageWriter, cancellationToken);
        else
            await HandleNonStreamingResponse(httpResponse, textSegmentWriter, completeMessageWriter, cancellationToken);
    }

    async Task HandleNonStreamingResponse(HttpResponseMessage httpResponse, Func<string,CancellationToken,Task> textSegmentWriter, Func<IMessage,CancellationToken,Task> completeMessageWriter, CancellationToken cancellationToken)
    {
        var readAsStreamAsync = await httpResponse.Content.ReadAsStreamAsync(cancellationToken: cancellationToken);
        var doc = await JsonDocument.ParseAsync(readAsStreamAsync, cancellationToken: cancellationToken);
        if (!doc.RootElement.TryGetProperty("object", out var objectElement))
            throw new HttpRequestException($"{Identifier}: Could not find object in response");
        if (objectElement.GetString() != "chat.completion")
            throw new HttpRequestException($"{Identifier}: expected chat.completion object but got {objectElement.GetString()}");
        if (!doc.RootElement.TryGetProperty("choices", out var choicesElement))
            throw new HttpRequestException($"{Identifier}: Could not find choices in response");
        var firstChoice = choicesElement.EnumerateArray().FirstOrDefault(); 
        
        if (!firstChoice.TryGetProperty("message", out var messageElement))
            throw new HttpRequestException($"{Identifier}: Could not find message in first choice in response");
        if (!messageElement.TryGetProperty("content", out var contentElement))
            throw new HttpRequestException($"{Identifier}: Could not find content in message in first choice in response");
        var content = contentElement.GetString() ?? throw new HttpRequestException($"{Identifier}: Could not find content in message in first choice in response");
        if (!messageElement.TryGetProperty("role", out var roleElement))
            throw new HttpRequestException("no role element in message in first choice in response");
        var role = roleElement.GetString() ?? throw new HttpRequestException($"{Identifier}: Could not find role in first choice in response");
        
        await textSegmentWriter(content, cancellationToken);
        await completeMessageWriter(new ChatMessage(role, content), cancellationToken);
    }

    static async Task HandleStreamingResponse(HttpResponseMessage httpResponse,
        Func<string, CancellationToken, Task> textSegmentWriter,
        Func<IMessage, CancellationToken, Task> completeMessageWriter,
        CancellationToken cancellationToken)
    {
        var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        
        if (!httpResponse.IsSuccessStatusCode)
        {
            var body = await new StreamReader(stream).ReadToEndAsync(cancellationToken);
            throw new HttpRequestException(body, null, httpResponse.StatusCode);
        }
        
        var sseParser = SseParser.Create<ChunkOrDone>(stream, (_, bytes) => bytes.SequenceEqual("[DONE]"u8) ?
            new() { Done = true } :
            new() { Json = JsonSerializer.Deserialize<JsonElement>(bytes) });

        Dictionary<int, (JsonElement, StringBuilder)> functionInvocations = new();

        var currentTextMessage = new StringBuilder();
        
        await foreach (var chunkOrDone in sseParser.EnumerateAsync(cancellationToken))
        {
            if (chunkOrDone.Data.Done)
                break;

            if (!chunkOrDone.Data.Json.TryGetProperty("choices", out var choices))
                throw new Exception("no choices");

            var firstChoice = choices.EnumerateArray().FirstOrDefault();
            if (firstChoice.ValueKind == JsonValueKind.Undefined)
                continue;

            if (!firstChoice.TryGetProperty("delta", out var delta))
                continue;

            if (delta.TryGetProperty("content", out var content))
            {
                var contentString = content.GetString();
                if (!string.IsNullOrEmpty(contentString))
                {
                    currentTextMessage.Append(contentString);
                    await textSegmentWriter(contentString, cancellationToken);
                }
            }
            
            if (delta.TryGetProperty("tool_calls", out var toolCalls))
            {
                await EmitChatMessageIfRequired();
                    
                foreach (var toolCall in toolCalls.EnumerateArray())
                {
                    if (!toolCall.TryGetProperty("index", out var indexElement))
                        continue;

                    if (indexElement.ValueKind != JsonValueKind.Number)
                        throw new ArgumentException();
                    var index = indexElement.GetInt32();

                    if (!toolCall.TryGetProperty("function", out var functionValue))
                        throw new ArgumentException();

                    if (!functionValue.TryGetProperty("arguments", out var argumentsValue))
                        throw new ArgumentException();

                    if (argumentsValue.ValueKind != JsonValueKind.String)
                        throw new ArgumentException();

                    if (!functionInvocations.TryGetValue(index, out var combinedFunctionInvocation))
                    {
                        functionInvocations[index] = (toolCall, new StringBuilder(argumentsValue.GetString()));
                        continue;
                    }

                    combinedFunctionInvocation.Item2.Append(argumentsValue.GetString());
                }
            }
        }

        await EmitChatMessageIfRequired();
        
        foreach (var (jo, sb) in functionInvocations.Values)
        {
            if (!jo.TryGetProperty("function", out var functionElement))
                throw new ArgumentException();
            if (!jo.TryGetProperty("id", out var idElement))
                throw new ArgumentException();
            if (!functionElement.TryGetProperty("name", out var nameElement))
                throw new ArgumentException();
            
            var id = idElement.GetString() ?? throw new ArgumentException();
            var name = nameElement.GetString() ?? throw new ArgumentException();
            
            var functionInvocation = new FunctionInvocation(id, name, JsonDocument.Parse(sb.ToString()));
            
            await completeMessageWriter(functionInvocation, cancellationToken);
        }

        return;
        
        async Task EmitChatMessageIfRequired()
        {
            if (currentTextMessage.Length == 0)
                return;
            await completeMessageWriter(new ChatMessage("assistant", currentTextMessage.ToString()), cancellationToken);
            currentTextMessage.Clear();
        }
    }


    protected virtual JsonObject JsonElementForRequest(ChatRequest request)
    {
        var messages = new JsonArray();
        if (request.SystemPrompt != null)
        {
            messages.Add(new JsonObject()
            {
                ["role"] = "system",
                ["content"] = request.SystemPrompt
            });
        }
        
        foreach(var m in request.Messages)
        {
            var jsonObjectFor = JsonObjectFor(m);
            messages.Add(jsonObjectFor);
        }

        JsonArray tools = [..request.Functions.Select(ToolFor)];
        var jsonElementForRequest = new JsonObject()
        {
            ["model"] = Identifier,
            ["temperature"] = request.Temperature,
            ["messages"] = messages,
            ["stream"] = supportsStreaming,
            ["user"] = request.EndUserIdentifier,
        };
        if (tools.Any())
            jsonElementForRequest["tools"] = tools;
        if (request.ResponseFormat != null)
            jsonElementForRequest["response_format"] = request.ResponseFormat.ToString();
        if (request.MaxTokens != null)
            jsonElementForRequest["max_tokens"] = request.MaxTokens;
        if (request.MandatoryFunction != null)
            jsonElementForRequest["tool_choice"] = new JsonObject()
            {
                ["type"] = "function", 
                ["function"] = new JsonObject() { ["name"] = request.MandatoryFunction.Name }
            };
            
        return jsonElementForRequest;

        static JsonObject ToolFor(Function f) => new()
        {
            ["type"] = "function",
            ["function"] = new JsonObject()
            {
                ["name"] = f.Name,
                ["description"] = f.Description,
                ["parameters"] = f.InputSchema.Deserialize<JsonObject>()
            }
        };

        static JsonObject JsonObjectFor(IMessage m) => m switch
        {
            ChatMessage cm => new()
            {
                ["role"] = cm.Role,
                ["content"] = cm.Text
            },
            ImageMessage im => new()
            {
                ["role"] = im.Role,
                ["content"] = new JsonArray()
                {
                    new JsonObject {
                        ["type"] = "image_url", 
                        ["image_url"] = new JsonObject()
                        {
                            ["url"] = $"data:{im.MimeType};base64,"+im.Data
                        }
                    }
                }
            },
            FunctionInvocation fi => new()
            {
                ["role"] = "assistant",
                ["content"] = null,
                ["tool_calls"] = new JsonArray()
                {
                    new JsonObject{
                        ["type"] = "function",
                        ["id"] = fi.Id,
                        ["function"] = new JsonObject()
                        {
                            ["name"] = fi.Name,
                            ["arguments"] = JsonSerializer.Serialize(fi.Parameters.RootElement)
                        }
                    }
                }
            },
            FunctionReturnValue frv => new()
            {
                ["role"] = "tool",
                ["tool_call_id"] = frv.Id,
                ["content"] = frv.Result,
            },
            _ => throw new NotSupportedException()
        };
    }

    protected abstract string Url { get; }
}


