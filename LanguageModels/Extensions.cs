using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace LanguageModels;

public static class Extensions
{
    public static string RemoveIfStartWith(this string text, string search)
    {
        var pos = text.IndexOf(search, StringComparison.Ordinal);
        return pos != 0 ? text : text.Substring(search.Length);
    }
    public static string GetMandatory(this IConfiguration config, string keyname) => config[keyname] ?? throw new Exception($"No config found for {keyname}");

    // public static async Task<ChatResponse> NonStreamedOverStreamed(this ILanguageModel lm, ChatRequest request, CancellationToken cancellationToken)
    // {
    //     var total = new StringBuilder();
    //     await foreach (var chatResponse in lm.ResponseForStreamedAsync(request, cancellationToken))
    //     {
    //         total.Append(chatResponse.Text);
    //     }
    //
    //     return new(total.ToString());
    // }

    public static async Task<string> ConcatenateAll(this IAsyncEnumerable<string> iae)
    {
        var sb = new StringBuilder();
        await foreach (var s in iae) 
            sb.Append(s);

        return sb.ToString();
    }

    public static async Task<T[]> ReadAll<T>(this IAsyncEnumerable<T> iae)
    {
        var result = new List<T>();
        await foreach(var e in iae)
            result.Add(e);
        return result.ToArray();
    }

    public static void Add(this JsonArray array, IEnumerable<JsonObject> objects)
    {
        foreach(var o in objects)
            array.Add(o);
    }
}