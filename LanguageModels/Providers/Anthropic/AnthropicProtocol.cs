using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LanguageModels;

public static class AnthropicProtocol
{
    public static JsonObject RequestObjectFor(ChatRequest request, string? model)
    {
        var newMessages = new List<IMessage>(request.Messages);
        
        if (request.ResponseFormat == ResponseFormat.Json)
            newMessages.Add(new ChatMessage(Role:"assistant", Text:"{"));
        
        JsonArray tools = [..request.Functions.Select(ToolFor)];
        var result = new JsonObject()
        {
            ["messages"] = new JsonArray(AnthropicMessagesFor(newMessages).ToArray()),
            ["max_tokens"] = 4096,
            ["stream"] = true,
            ["tools"] = tools
        };

        if (model != null)
            result["model"] = model;

        if (request.MandatoryFunction != null)
            result["tool_choice"] = new JsonObject()
            {
                ["type"] = "tool",
                ["name"] = request.MandatoryFunction.Name
            };
        
        var systemMessage = request.SystemPrompt;
        if (systemMessage != null)
            result["system"] = request.SystemPrompt;
        return result;

        static JsonObject ToolFor(Function f) => new()
        {
            ["name"] = f.Name,
            ["description"] = f.Description,
            ["input_schema"] = f.InputSchema.Deserialize<JsonObject>()
        };
    }

    static JsonObject[] ContentBlocksFor(IMessage message) => message switch
    {
        ChatMessage cm =>
        [
            new()
            {
                ["type"] = "text",
                ["text"] = cm.Text
            }
        ],
        ImageMessage im =>
        [
            new()
            {
                ["type"] = "image",
                ["source"] = new JsonObject()
                {
                    ["type"] = "base64",
                    ["media_type"] = im.MimeType,
                    ["data"] = im.Data
                }
            }
        ],
        FunctionReturnValue frv =>
        [
            new()
            {
                ["type"] = "tool_result",
                ["tool_use_id"] = frv.Id,
                ["is_error"] = !frv.Successful,
                ["content"] = new JsonArray()
                {
                    new JsonObject()
                    {
                        ["type"] = "text",
                        ["text"] = frv.Result
                    }
                }
            }
        ],
        FunctionInvocation fi =>
        [
            new()
            {
                ["type"] = "tool_use",
                ["id"] = fi.Id,
                ["name"] = fi.Name,
                ["input"] = fi.Parameters.Deserialize<JsonObject>()
            }
        ],
        _ => throw new ArgumentException()
    };

    //anthropic is very precise about not allowing double user messages or double assistant messages. it does support
    //multiple content blocks in a message, so we're going to merge messages with identical roles into single messages
    //with their content blocks merged.
    static IEnumerable<IMessage[]> MergeMessagesWithIdenticalRoles(IEnumerable<IMessage> messages)
    {
        List<IMessage> currentBatch = [];

        foreach (var message in messages)
        {
            if (currentBatch.Count == 0 || AnthropicRoleFor(message) == AnthropicRoleFor(currentBatch.Last()))
            {
                currentBatch.Add(message);
                continue;
            }

            yield return currentBatch.ToArray();
            currentBatch.Clear();
            currentBatch.Add(message);
        }

        if (currentBatch.Any())
            yield return currentBatch.ToArray();
    }

    static string AnthropicRoleFor(IMessage m) => m switch
    {
        ImageMessage im => im.Role,
        ChatMessage cm => cm.Role,
        FunctionReturnValue => "user",
        FunctionInvocation => "assistant",
        _ => throw new ArgumentException()
    };

    static IEnumerable<JsonNode> AnthropicMessagesFor(List<IMessage> newMessages)
    {
        return MergeMessagesWithIdenticalRoles(newMessages).Select(messageSet =>
        {
            var selectMany = messageSet.SelectMany(ContentBlocksFor).ToArray<JsonNode>();
            
            return new JsonObject()
            {
                ["role"] = AnthropicRoleFor(messageSet[0]),
                ["content"] = new JsonArray(selectMany)
            };
        });
    }

    public static async Task ProcessResponse(ChatRequest request, IAsyncEnumerable<JsonElement> sseStream,
        Func<string, CancellationToken, Task> textWriter,
        Func<IMessage, CancellationToken, Task> completeMessagesChannelWriter,
        CancellationToken cancellationToken)
    {
        string? currentFunctionInvocationName = null;
        string? currentFunctionInvocationId = null;
        StringBuilder currentFunctionInvocationArguments = new();
        StringBuilder currentTextMessage = new();

        if (request.ResponseFormat == ResponseFormat.Json)
            await textWriter("{", cancellationToken);

        
        //todo: correctly handle this error anthropic gave me during an outage:
        //ValueKind = Object : "{"type":"error","error":{"details":null,"type":"overloaded_error","message":"Overloaded"}              }"
        
        await foreach (var sseItemData in sseStream)
        {
            if (!sseItemData.TryGetProperty("type", out var eventTypeElement))
                throw new ArgumentException();

            var sseItemEventType = eventTypeElement.GetString();
            switch (sseItemEventType)
            {
                case "content_block_start":
                    if (!sseItemData.TryGetProperty("content_block", out var contentBlockElement))
                        throw new ArgumentException();

                    if (!contentBlockElement.TryGetProperty("type", out var contentBlockTypeElement))
                        throw new ArgumentException();
                    
                    if (contentBlockTypeElement.GetString() == "tool_use")
                    {
                        if (!contentBlockElement.TryGetProperty("id", out var idElement))
                            throw new ArgumentException();
                        currentFunctionInvocationId = idElement.GetString() ?? throw new ArgumentException();

                        if (!contentBlockElement.TryGetProperty("name", out var nameElement))
                            throw new ArgumentException();
                        currentFunctionInvocationName = nameElement.GetString() ?? throw new ArgumentException();
                    }

                    if (contentBlockTypeElement.GetString() == "text")
                    {
                        if (!contentBlockElement.TryGetProperty("text", out var textElement2))
                            throw new ArgumentException();
                        var text = textElement2.GetString() ?? throw new ArgumentException();
                        if (text.Length > 0)
                        {
                            currentTextMessage.Append(text);
                            await textWriter(text, cancellationToken);
                        }
                    }
                    break;
                
                case "content_block_stop":
                    if (currentTextMessage.Length > 0)
                    {
                        var chatMessage = new ChatMessage("assistant", currentTextMessage.ToString());
                        currentTextMessage.Clear();
                        await completeMessagesChannelWriter(chatMessage, cancellationToken);
                    }

                    if (currentFunctionInvocationName != null)
                    {
                        var fi = new FunctionInvocation(
                            Id: currentFunctionInvocationId ?? throw new ArgumentException(),
                            Name: currentFunctionInvocationName ?? throw new ArgumentException(),
                            Parameters: JsonDocument.Parse(currentFunctionInvocationArguments.ToString()));

                        currentFunctionInvocationId = null;
                        currentFunctionInvocationName = null;
                        currentFunctionInvocationArguments.Clear();

                        await completeMessagesChannelWriter(fi, cancellationToken);
                    }
                    break;

                case "content_block_delta":
                    if (!sseItemData.TryGetProperty("delta", out var deltaElement))
                        break;
                    if (!deltaElement.TryGetProperty("type", out var typeElement))
                        break;
                    if (typeElement.GetString() == "text_delta")
                    {
                        if (!deltaElement.TryGetProperty("text", out var textElement))
                            throw new ArgumentException();

                        var text = textElement.GetString() ?? throw new ArgumentException();
                        if (!string.IsNullOrEmpty(text))
                        {
                            currentTextMessage.Append(text);
                            await textWriter(text, cancellationToken);
                        }
                    }

                    if (typeElement.GetString() == "input_json_delta")
                    {
                        if (!deltaElement.TryGetProperty("partial_json", out var partialJsonElement))
                            throw new ArgumentException();
                        currentFunctionInvocationArguments.Append(partialJsonElement.GetString() ?? throw new ArgumentException());
                    }

                    break;
                
                case "message_stop":
                    //ready!
                    return;
                
                case "ping":
                case "message_start":
                case "message_delta":
                default:
                    //do nothing
                    break;
            }
        }
    }
}