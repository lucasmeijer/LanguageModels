namespace LanguageModels;

public class GroqModels(GroqHttpClient httpClient)
{
    public ILanguageModel LLama3_70b { get; } = new Groq(httpClient, "llama3-70b-8192");
}