using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace LanguageModels.Tests;

[TestFixture]
public abstract class LanguageModelTestBase
{
    protected ServiceProvider ServiceProvider { get; private set; } = null!;
    protected IConfiguration Configuration { get; } = new ConfigurationBuilder().AddUserSecrets<LanguageModelTestBase>().Build();

    ServiceProvider MakeServiceProvider()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLanguageModels();
        serviceCollection.AddSingleton(Configuration);
        SetupLanguageModelIn(serviceCollection);
        return serviceCollection.BuildServiceProvider();
    }

    [SetUp]
    public void SetUp()
    {
        ServiceProvider = MakeServiceProvider();
    }
    protected abstract void SetupLanguageModelIn(IServiceCollection serviceCollection);
    
    [Test]
    public async Task SimpleChatMessage()
    {
        var model = ServiceProvider.GetRequiredService<ILanguageModel>();
        
        var r = model.Execute(SimpleChatRequest, CancellationToken.None);
        var readTextSegmentsResult = await r.ReadTextSegmentsAsync().ConcatenateAll();
        var messages = await r.ReadCompleteMessagesAsync().ReadAll();
        await r.DisposeAsync();
        Assert.Greater(readTextSegmentsResult.Length, 10);

        var chatMessage = messages.OfType<ChatMessage>().Single();
        Assert.That(chatMessage.Text.Length, Is.EqualTo(readTextSegmentsResult.Length));
        
        Console.WriteLine(readTextSegmentsResult);
    }


    [Test]
    public async Task AnalyzeImage()
    {
        var model = MakeServiceProvider().GetRequiredService<ILanguageModel>();

        if (!model.SupportImageInputs)
            Assert.Ignore("This model does not support image inputs");
        
        var bytes = await File.ReadAllBytesAsync("/Users/lucas/Desktop/oud/alfred_pennyworth_by_arunion_db5z41t-pre.jpg");
        
        await using var r = model.Execute(new()
        {
            SystemPrompt = "Your job is to report what is in an image",
            Messages = [new ImageMessage("user", "image/jpeg", Convert.ToBase64String(bytes))],
        }, CancellationToken.None);

        var response = await r.ReadTextSegmentsAsync().ConcatenateAll();
        Console.WriteLine(response);
    }
    
    [Test]
    public async Task AnalyzeTwoImages()
    {
        var model = MakeServiceProvider().GetRequiredService<ILanguageModel>();

        if (!model.SupportImageInputs)
            Assert.Ignore("This model does not support image inputs");
        
        var bytes1 = await File.ReadAllBytesAsync("/Users/lucas/Desktop/oud/alfred_pennyworth_by_arunion_db5z41t-pre.jpg");
        var bytes2 = await File.ReadAllBytesAsync("/Users/lucas/stukkebrief2.jpg");
        
        await using var r = model.Execute(new()
        {
            SystemPrompt = "Your job is to report what is in these two images",
            Messages = [
                new ImageMessage("user", "image/jpeg", Convert.ToBase64String(bytes1)),
                new ImageMessage("user", "image/jpeg", Convert.ToBase64String(bytes2))
            ],
            
        }, CancellationToken.None);

        var response = await r.ReadTextSegmentsAsync().ConcatenateAll();
        Console.WriteLine(response);
    }
    
    [Test]
    public async Task FunctionInvocation()
    {
        var model = MakeServiceProvider().GetRequiredService<ILanguageModel>();

        var firstRequest = new ChatRequest()
        {
            Messages = [
                new ChatMessage(Role: "user", Text: "whats the weather like in amsterdam")
            ],
            Functions = [
                new(Name: "get_weather",
                    Description: "returns the weather at the requested location",
                    InputSchema: JsonDocument.Parse("""
                                                    {
                                                      "type": "object",
                                                      "properties": {
                                                        "location" : {
                                                          "type": "string"
                                                        }
                                                      }
                                                    }
                                                    """),
                    false,
                    Implementation: null)
            ],
        };

        FunctionInvocation fi;
        {
            await using var firstExecution = model.Execute(request: firstRequest, default);
            var readAll = await firstExecution.ReadCompleteMessagesAsync().ReadAll();
            var fis = readAll
                .OfType<FunctionInvocation>()
                .ToArray();

            Assert.That(fis.Length, Is.EqualTo(1));
            fi = fis.Single();
            Assert.That(fi.Name, Is.EqualTo("get_weather"));
            Assert.IsTrue(fi.Parameters.RootElement.TryGetProperty("location", out var locationElement));
            Assert.That(locationElement.GetString()?.ToLower(), Is.EqualTo("amsterdam"));
        }

        var secondRequest = firstRequest with
        {
            Messages =
            [
                ..firstRequest.Messages,
                fi,
                new FunctionReturnValue(fi.Id, true, "20 degrees celcius")
            ]
        };
        await using var secondExecution = model.Execute(secondRequest, default);
        var result = await secondExecution.ReadTextSegmentsAsync().ConcatenateAll();
        Console.WriteLine(result);
        StringAssert.Contains("20", result);
    }
    
    [Test]
    public async Task CSharpBackedFunction()
    {
        var model = MakeServiceProvider().GetRequiredService<ILanguageModel>();

        IMessage[] allMessages;
        {
            await using var execution = model.Execute(new()
            {
                Messages =
                [
                    new ChatMessage("user", """
                                            I want to test your function calling capabilities. Call function func with these arguments:
                                            
                                            mystring => hello
                                            myint => 123
                                            myfloat => 501
                                            mybool => true
                                            record.nested_string => nested
                                            record2.nest.nested_string => doublenest
                                            record3.enum => B
                                            record3.array => [23, 42, 1024]
                                            record3.field => foo
                                            record3.recursive[0].field => bar
                                            record3.recursive[0].recursive => []
                                            """)
                ],
                Functions = CSharpBackedFunctions.Create([new TestFunction()])
            }, default);
            allMessages = await execution.ReadCompleteMessagesAsync().ReadAll();
        }
        var functionReturnValue = allMessages.OfType<FunctionReturnValue>().Single();
        if (!functionReturnValue.Successful)
            throw new AssertionException($"FunctionInvocation was not succesful: {functionReturnValue.Result}");
    }
    
    class TestFunction()
    {
        [DescriptionForLanguageModel("test function to invoke")]
        public Task<string> Func(string MyString, int MYINT, float myfloat, bool mybool, MyRecord record, MyNested2 record2, MyComplexRecord record3)
        {
            if (MyString != "hello")
                throw new ArgumentException($"mystring was {MyString}");
            if (MYINT != 123)
                throw new ArgumentException($"myint was {MYINT}");
            if (myfloat is < 500 or > 502)
                throw new ArgumentException($"myfloat was {myfloat}");
            if (mybool != true)
                throw new ArgumentException("mybool was false!");
            if (record.NestedString != "nested")
                throw new ArgumentException($"record.NestedString was {record.NestedString}!");
            if (record2.nest.NestedString != "doublenest")
                throw new ArgumentException($"record2.nest.NestedString was {record2.nest.NestedString}!");
            if (record3.Enum != MyEnum.B)
                throw new ArgumentException($"record3.Enum was {record3.Enum}!");
            if (record3.Array.Length != 3)
                throw new ArgumentException($"record3.Array.Length was {record3.Array.Length}!");
            if (record3.Field != "foo")
                throw new ArgumentException($"record3.Field was {record3.Field}!");
            if (record3.Recursive.Length != 1)
                throw new ArgumentException($"record3.Recursive.Length was {record3.Array.Length}!");
            if (record3.Recursive[0].Field != "bar")
                throw new ArgumentException($"record3.Recursive[0].Field was {record3.Recursive[0].Field}!");
            if (record3.Recursive[0].Recursive.Length != 0)
                throw new ArgumentException($"record3.Recursive[0].Recursive.Length was {record3.Recursive[0].Recursive.Length}!");

            return Task.FromResult("All arguments were passed correctly!");
        }

        internal record MyRecord(string NestedString);
        internal record MyNested2(MyRecord nest);

        internal enum MyEnum { A, B, C }
        
        internal record MyComplexRecord
        {
            public MyEnum Enum { get; set; }
            public int[] Array { get; set; } = [];
            [JsonInclude]
            public string Field = "";
            public MyComplexRecord[] Recursive { get; set; } = [];
        }
    }
    

    public static ChatRequest SimpleChatRequest => new() { Messages = [new ChatMessage("user", "write a joke about a horse")] };
}