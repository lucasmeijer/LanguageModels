using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime.EventStreams;
using Amazon.Runtime.Internal.Transform;

namespace LanguageModels;

public class BedrockModels(AmazonBedrockRuntimeClient client)
{
    public ILanguageModel Sonnet35 { get; } = new BedrockModel(client, "anthropic.claude-3-sonnet-20240229-v1:0");
}

class BedrockModel(AmazonBedrockRuntimeClient amazonBedrockRuntimeClient, string model) : ILanguageModel
{ 
    public string Identifier => model;
    public IExecutionInProgress Execute(ChatRequest request, CancellationToken cancellationToken)
    {
        return new ExecutionInProgress(request, ExecuteImpl);
    }

    public static class KnownModel
    {
        public static string Sonnet35 => "anthropic.claude-3-sonnet-20240229-v1:0";
    }
    
    async Task ExecuteImpl(ChatRequest chatRequest, Func<string, CancellationToken, Task> textSegmentWriter, Func<IMessage, CancellationToken, Task> completeMessageWriter, CancellationToken cancellationToken)
    {
        var requestObjectFor = AnthropicProtocol.RequestObjectFor(chatRequest, null);
        requestObjectFor["anthropic_version"] = "bedrock-2023-05-31";
        requestObjectFor.Remove("stream");

        var invokeModelWithResponseStreamRequest = new InvokeModelWithResponseStreamRequest()
        {
            Body = new(Encoding.UTF8.GetBytes(requestObjectFor.ToJsonString())),
            Accept = "application/json",
            ContentType = "application/json",
            ModelId = model
        };
        
        var awsResponse = await amazonBedrockRuntimeClient.InvokeModelWithResponseStreamAsync(invokeModelWithResponseStreamRequest, cancellationToken);
        
        await AnthropicProtocol.ProcessResponse(chatRequest, ProcessResult(awsResponse, cancellationToken), textSegmentWriter, completeMessageWriter, cancellationToken);
        return;

        static async IAsyncEnumerable<PayloadPart> PayloadPartsFrom(InvokeModelWithResponseStreamResponse r)
        {
            ConcurrentQueue<PayloadPart> queue = new();

            using SemaphoreSlim semaphore = new(0);
            
            void OnBodyOnChunkReceived(object? sender, EventStreamEventReceivedArgs<PayloadPart> args)
            {
                queue.Enqueue(args.EventStreamEvent);
                semaphore.Release();
            }
            
            r.Body.ChunkReceived += OnBodyOnChunkReceived; 
            var finishTask = r.Body.StartProcessingAsync();

            while (true)
            {
                var t = await Task.WhenAny(semaphore.WaitAsync(), finishTask);
                if (t == finishTask)
                    yield break;
                while (queue.TryDequeue(out var p))
                    yield return p;
            }
        }
        
        static async IAsyncEnumerable<JsonElement> ProcessResult(InvokeModelWithResponseStreamResponse r, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var pp in PayloadPartsFrom(r))
            {
                JsonDocument jsonDocument = await JsonDocument.ParseAsync(pp.Bytes, cancellationToken: cancellationToken) ?? throw new ArgumentException();
                yield return jsonDocument.RootElement;
            }
        }
    }

    public bool SupportFunctionCalls => true;
    public bool SupportImageInputs => true;
}