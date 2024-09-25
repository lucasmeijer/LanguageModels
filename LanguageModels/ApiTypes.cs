using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace LanguageModels;

public interface ILanguageModel
{
    string Identifier { get; }
    IExecutionInProgress Execute(ChatRequest request, CancellationToken cancellationToken);

    public bool SupportFunctionCalls { get; }
    public bool SupportImageInputs { get; }
}

public interface IExecutionInProgress : IAsyncDisposable
{
    IAsyncEnumerable<string> ReadTextSegmentsAsync();
    IAsyncEnumerable<IMessage> ReadCompleteMessagesAsync();
}

public enum ResponseFormat
{
    Json
}

public record ChatRequest
{
    public string? SystemPrompt { get; set; }
    public required IMessage[] Messages { get; init; }
    public ResponseFormat? ResponseFormat { get; init; } = null;
    public float Temperature { get; init; } = 0;
    public int? MaxTokens { get; init; } = null;
    public Function[] Functions { get; init; } = [];
    public Function? MandatoryFunction { get; init; } = null;
    public string? EndUserIdentifier { get; init; }
    public Func<FunctionInvocation, Function, Task<bool>>? FunctionApproval { get; init; }
}

public record Function(string Name, string? Description, JsonDocument InputSchema, bool RequiresExplicitApproval, Func<JsonDocument, Task<string>>? Implementation);

public record FunctionInvocation(string Id, string Name, JsonDocument Parameters) : IMessage;
public record FunctionReturnValue(string Id, bool Successful, string Result) : IMessage;

[JsonDerivedType(typeof(ChatMessage))]
[JsonDerivedType(typeof(FunctionReturnValue))]
[JsonDerivedType(typeof(FunctionInvocation))]
[JsonDerivedType(typeof(ImageMessage))]
public interface IMessage;

public record ChatMessage(string Role, string Text) : IMessage;

public record ImageMessage(string Role, string MimeType, string Data) : IMessage;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter, Inherited = false)]
[MeansImplicitUse]
public sealed class DescriptionForLanguageModel(string description) : Attribute
{
    public string Description { get; } = description;
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
[MeansImplicitUse]
public sealed class RequiresExplicitApproval() : Attribute;

public delegate Task ResponseParsingFunc(ChatRequest chatRequest, Func<string, CancellationToken, Task> textSegmentWriter, Func<IMessage, CancellationToken, Task> completeMessageWriter, CancellationToken cancellationToken);