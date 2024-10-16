using System.Threading.Channels;

namespace LanguageModels;

class ExecutionInProgress : IExecutionInProgress
{
    Task _responseParsingTask;
    CancellationTokenSource _cts = new();

    readonly Channel<string> _textSegmentsChannel = Channel.CreateUnbounded<string>(_unboundedChannelOptions);
    readonly Channel<IMessage> _completeMessagesChannel = Channel.CreateUnbounded<IMessage>(_unboundedChannelOptions);

    static UnboundedChannelOptions _unboundedChannelOptions = new()
    {
        SingleWriter = true,
        SingleReader = true,
        AllowSynchronousContinuations = true
    };

    public ExecutionInProgress(ChatRequest chatRequest, CancellationToken cancellationToken, ResponseParsingFunc responseParser)
    {
        _responseParsingTask = ExecutionInProgressImpl(chatRequest, cancellationToken, responseParser);
    }
    
    async Task ExecutionInProgressImpl(ChatRequest request, CancellationToken cancellationToken, ResponseParsingFunc responseParsingFunc)
    {
        try
        {
            await StartResponseParsingTask(request, responseParsingFunc, cancellationToken);
        }
        finally
        {
            _textSegmentsChannel.Writer.Complete();
            _completeMessagesChannel.Writer.Complete();    
        }
    }

    async Task StartResponseParsingTask(
        ChatRequest request,
        ResponseParsingFunc responseParsingFunc, 
        CancellationToken cancellationToken)
    {
        try
        {
            var receivedMessages = new List<IMessage>();
            var returnValueTasks = new List<Task<FunctionReturnValue?>>();

            async Task TextSegmentWriter(string s, CancellationToken token)
            {
                await _textSegmentsChannel.Writer.WriteAsync(s, token);
            }

            async Task CompleteMessageWriter(IMessage message, CancellationToken token)
            {
                receivedMessages.Add(message);
                if (message is FunctionInvocation fi)
                {
                    returnValueTasks.Add(ResponseTaskFor(request.Functions, fi, request.FunctionApproval));
                }

                await _completeMessagesChannel.Writer.WriteAsync(message, token);
            }

            await responseParsingFunc(request, TextSegmentWriter, CompleteMessageWriter, cancellationToken);

            var returnValues = (await Task.WhenAll(returnValueTasks))
                .Where(v => v is not null)
                .Select(v => v!)
                .ToArray();

            foreach (var returnValue in returnValues)
                await _completeMessagesChannel.Writer.WriteAsync(returnValue, cancellationToken);

            if (returnValues.Any())
            {
                await StartResponseParsingTask(request with
                {
                    Messages =
                    [
                        ..request.Messages,
                        ..SortReceivedMessagesAndReturnValues(receivedMessages, returnValues)
                    ]
                }, responseParsingFunc, cancellationToken);
            }
        }
        catch (TaskCanceledException)
        {
        }
    }

    // OpenAI wants return values to come right as the next message after function calls, so we sort them.
    private IEnumerable<IMessage> SortReceivedMessagesAndReturnValues(List<IMessage> receivedMessages, FunctionReturnValue[] returnValues)
    {
        foreach (var m in receivedMessages)
        {
            yield return m;
            if (m is FunctionInvocation fi)
                yield return returnValues.Single(rv => rv.Id == fi.Id);
        }
    }

    static async Task<FunctionReturnValue?> ResponseTaskFor(
        Function[] functions,
        FunctionInvocation fi,
        Func<FunctionInvocation, Function, Task<bool>>? chatRequestFunctionApproval)
    {
        var function = functions.FirstOrDefault(f => f.Name == fi.Name);
        if (function?.Implementation == null)
            return null;

        if (function.RequiresExplicitApproval)
        {
            if (chatRequestFunctionApproval == null)
                throw new ArgumentException($"{fi.Name} requires explicit approval, but no approval mechanism was provided");

            try
            {
                if (!await chatRequestFunctionApproval(fi, function))
                    return new(fi.Id, false, "user manually declined function invocation request");
            }
            catch (Exception e)
            {
                return new(fi.Id, false, $"user wanted to manually approve this request, but an exception happened during approval: {e}");
            }
        }

        try
        {
            return new(fi.Id, true, await function.Implementation!(fi.Parameters));
        }
        catch (Exception e)
        {
            return new(fi.Id, false, e.Message);
        }
    }



    async IAsyncEnumerable<T> ReadAllAsyncAndThrowIfUnderlyingTaskThrows<T>(ChannelReader<T> reader, Task underlyingTask) 
    {
        await foreach (var segment in reader.ReadAllAsync())
        {
            yield return segment;
        }

        await underlyingTask;
    }
    
    public IAsyncEnumerable<string> ReadTextSegmentsAsync() => ReadAllAsyncAndThrowIfUnderlyingTaskThrows<string>(_textSegmentsChannel, _responseParsingTask);
    public IAsyncEnumerable<IMessage> ReadCompleteMessagesAsync() => ReadAllAsyncAndThrowIfUnderlyingTaskThrows<IMessage>(_completeMessagesChannel, _responseParsingTask); 

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try
        {
            await _responseParsingTask;
        }
        catch (TaskCanceledException)
        {
        }
    }
}